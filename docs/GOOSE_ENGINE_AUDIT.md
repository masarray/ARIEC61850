# GOOSE Engine Audit

Status date: 2026-06-14

## Why this exists

GOOSE support must become more than a frame encoder/decoder. For a commissioning tool, the useful behavior is SCL-aware discovery, stream supervision, event evidence, and safe publishing with protocol state that can be inspected.

This audit is based on public IEC 61850 process-bus behavior, public tool capability descriptions, and local validation.

No third-party IEC 61850 stack is vendored as a runtime dependency.

## Capability target

The target is a smart GOOSE stack that can support professional commissioning, simulation, and process-bus troubleshooting tools:

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

## N5.20 source patch

- Added `IProcessBusFrameSource`, `ProcessBusCapturedFrame`, and `ProcessBusCaptureOptions` so receive-side process-bus traffic is a typed core contract instead of app-specific SharpPcap logic.
- Added `NpcapProcessBusFrameSource` with bounded-channel delivery, adapter filter support, cancellation cleanup, and best-effort adapter shutdown.
- Added `goose-subscribe-live --adapter <index|name>` CLI. It captures GOOSE traffic with the default BPF filter `ether proto 0x88b8`, feeds frames into `ProcessBusStreamMonitor`, and prints the same SCL-aware event and summary diagnostics used by `stream-pcap`.
- Added deterministic in-memory frame-source tests for subscriber consumers.

Tests:

- SCL-bound GOOSE retransmission.
- Valid GOOSE state change with changed values.
- Invalid value change without state-number increment.
- TAL expiry.
- In-memory process-bus frame source ordering and cancellation.

## Comparison with common low-level stacks

Common low-level IEC 61850 stacks provide strong raw GOOSE receiver/subscriber APIs. A good baseline exposes filtering by destination MAC and APPID, validity and parse-error state, `stNum`, `sqNum`, `test`, `confRev`, `ndsCom`, TimeAllowedToLive, timestamp, VLAN, and DataSet values. The expected `stNum` and `sqNum` relationship is also fundamental: `sqNum` increases for consecutive messages without state change and resets when `stNum` increases.

ARIEC61850 is still behind mature low-level stacks in these areas:

- live GOOSE receiver loop is source-implemented but still needs compile validation in an unrestricted restore environment and live adapter soak evidence;
- no R-GOOSE receiver/publisher;
- no hardened multi-threaded live subscriber lifecycle;
- no formal conformance evidence;
- no broad multi-vendor traffic corpus.

ARIEC61850 is moving beyond a raw subscriber API in these areas:

- SCL-bound semantic diagnostics are part of the monitor result;
- value-change summaries are compared against the GOOSE state machine;
- ambiguous or anonymous traffic remains visible instead of being silently dropped;
- the same monitor model supports PCAP replay and live adapter receive;
- publisher dry-run and active publish share the same state-machine path.

## Passive analyzer learning

Receive-only raw-passive analyzers provide useful product lessons for ARIEC61850:

- show adapter, APPID, stream ID, source MAC, sequence continuity, and timing confidence as evidence;
- avoid claiming certification-grade timing from ordinary software timestamps;
- make passive discovery useful even without SCL, but mark semantics as anonymous when SCL binding is missing;
- keep active publishing and control behavior out of passive analyzer workflows.

ARIEC61850 now adopts the same evidence-first posture for GOOSE, while remaining a full stack that can also publish in isolated labs.

## Engineering tool learning

Professional engineering tools emphasize workflows that visualize SCL, trace signals, compare configuration with live traffic, inspect activity, and simulate IEDs/GOOSE for testing. The stack direction should therefore prioritize:

- SCL and live traffic comparison;
- clear PASS/WARNING/FAIL/UNKNOWN diagnostics per stream;
- signal tracing from GoCB/DataSet member to received `allData` value;
- simulation/publishing that is deterministic and bounded;
- evidence export for commissioning review.

## Current validation

Commands run for N5.19:

```powershell
dotnet build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release --no-restore --no-incremental
dotnet build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-restore --no-incremental
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build
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

1. Validate `goose-subscribe-live` compile and live adapter capture in an unrestricted restore environment.
2. Add bounded capture evidence export: JSON summary, event log, and optional CSV values.
3. Add live MMS GoCB discovery/readback for `GoEna`, `GoID`, `DatSet`, `ConfRev`, `NdsCom`, `MinTime`, `MaxTime`, and `DstAddress`.
4. Add quality bit decoding for common GOOSE quality values.
5. Add replay tests with malformed GOOSE frames, unknown tags, length mismatch, sequence jumps, duplicate frames, and ConfRev mismatch.
6. Add anonymous stream registry for traffic without SCL and later bind it when SCL is loaded.
7. Add live adapter soak tests with real relay or simulator captures.
8. Add R-GOOSE only after normal Ethernet GOOSE receive/publish is stable.

## Claim boundary

Current validated claim: SCL-backed GOOSE frame generation, PCAP decode, passive stream monitoring, state/retransmission diagnostics, TAL supervision, and bounded lab publishing are implemented and unit tested.

Source-level and pending lab validation: live adapter GOOSE subscriber CLI and Npcap receive source.

Do not claim yet: formal IEC 61850 conformance, production-grade timing proof, live adapter subscriber soak readiness, R-GOOSE support, or broad vendor interoperability.
