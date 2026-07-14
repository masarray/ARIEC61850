# GOOSE Engine Review

**Status date:** 2026-06-14

## Purpose

This document records the implemented GOOSE capability, validation evidence, limitations, and next engineering steps. It is based on public IEC 61850 behavior, project-owned code, synthetic fixtures, and local validation.

No unrelated IEC 61850 implementation is included as a runtime dependency.

## Current capability

### Model and binding

- SCL-derived publisher profiles are accepted by `ProcessBusStreamMonitor`.
- Observed frames are matched by APPID, destination MAC, GoCB reference, DataSet reference, GoID, configuration revision, and other available evidence.
- Match outcomes should be described as exact, high-confidence, partial, mismatch, or anonymous rather than “trusted”.
- DataSet order is preserved when SCL evidence is available.
- Traffic without SCL remains visible and is labeled semantically anonymous.

### Decode and supervision

- `allData` values are decoded into typed entries with index, signal reference when known, FC, CDC, type, display value, previous value, and change state.
- Stream summaries track state changes, retransmissions, duplicates, sequence gaps, regressions, supervision timeout, arrival gaps, and decoded value changes.
- Diagnostics identify Test, needs-commissioning, zero TimeAllowedToLive, configuration mismatch, destination mismatch, DataSet count mismatch, timeout, and state/value inconsistency.

### Publisher

- Retransmission frames retain stable payload values.
- Payload changes are tied to state changes.
- `stNum` and `sqNum` behavior is deterministic and testable.
- Dry-run and bounded active publishing use the same state-machine path.

### Capture and CLI

- Receive-side process-bus traffic uses a typed frame-source abstraction.
- The Windows raw-Ethernet frame source supports adapter selection, filter options, bounded delivery, cancellation, and cleanup.
- CLI inspection and streaming show sequence state, supervision, model binding, decoded value count, changes, and diagnostics.

## Evidence model

```text
synthetic SCL
→ project-generated GOOSE frames or approved capture
→ decode and stream supervision
→ SCL binding
→ explicit findings
→ Markdown/JSON evidence
```

## Current automated coverage

- SCL-bound retransmission.
- State change with matching value change.
- Value change without state-number increment.
- State-number change without decoded value change.
- Supervision timeout.
- Sequence gaps, duplicates, and regressions.
- Configuration and destination mismatch.
- In-memory frame-source ordering and cancellation.

## Current limitations

- No formal IEC 61850 conformance evidence.
- No broad multi-implementation traffic corpus.
- No R-GOOSE support claim.
- Sustained live-adapter lifecycle and soak evidence remain limited.
- Ordinary software timestamps are laboratory evidence and are not presented as certification-grade timing.
- Full station-level dataflow validation remains under development.

## Product guidance

A useful engineering view should show:

- adapter and capture source;
- APPID and stream reference;
- source and destination MAC;
- expected-model match level;
- `stNum`, `sqNum`, supervision, and timing evidence;
- decoded value count and changes;
- clear MATCHED, PARTIAL, MISSING, UNEXPECTED, and MISMATCH findings.

Passive analysis should remain useful without SCL while clearly labeling unknown semantics. Active publishing must remain separate, visible, bounded, and guarded.

## Validation commands

```powershell
dotnet build .\apps\AR.Iec61850.Cli\AR.Iec61850.Cli.csproj -c Release
dotnet build .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release
dotnet test .\tests\AR.Iec61850.Tests\AR.Iec61850.Tests.csproj -c Release --no-build

dotnet run --project .\apps\AR.Iec61850.Cli -- generate-pcap .\samples\scl\minimal-station.scd .\.artifacts\out\goose-demo.pcap
dotnet run --project .\apps\AR.Iec61850.Cli -- inspect-pcap .\.artifacts\out\goose-demo.pcap --scl .\samples\scl\minimal-station.scd
dotnet run --project .\apps\AR.Iec61850.Cli -- stream-pcap .\.artifacts\out\goose-demo.pcap --scl .\samples\scl\minimal-station.scd --delay-ms 0 --limit 20
```

Generated evidence must stay under ignored local folders and use synthetic or contributor-owned input.

## Next steps

1. Add bounded live-capture evidence export.
2. Add live MMS GoCB discovery and readback where supported.
3. Expand quality decoding and malformed-frame coverage.
4. Add sustained adapter soak tests and explicit resource limits.
5. Add anonymous-stream rebinding after SCL is loaded.
6. Add station-level publisher/DataSet/subscriber tracing.
7. Consider R-GOOSE only after ordinary Ethernet GOOSE behavior is mature.

## Claim boundary

Current public claim: SCL-backed GOOSE encode/decode, project-generated PCAP workflows, passive stream monitoring, sequence and supervision diagnostics, and bounded laboratory publishing are implemented with automated coverage.

Do not claim formal conformance, production-grade timing, universal interoperability, unrestricted operational use, or R-GOOSE support.
