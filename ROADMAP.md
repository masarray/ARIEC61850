# ARIEC61850 Roadmap

ARIEC61850 is an independently developed IEC 61850 engineering stack for .NET. This roadmap describes future work only. Completed changes belong in `CHANGELOG.md`; current capability and evidence boundaries belong in `docs/ENGINE_MATURITY_MATRIX.md`.

## Current baseline

The current source provides:

- MMS client association, discovery, typed reads, guarded writes, reporting, and control-model-aware command sequencing;
- GOOSE and Sampled Values codecs, publishing, capture, and diagnostic profiles;
- SCL parsing and expected-vs-observed engineering analysis;
- deterministic simulation and a read-only MMS laboratory server;
- CLI and Windows workspaces for discovery, diagnostics, simulation, and evidence export.

This baseline is suitable for laboratory validation, engineering development, education, and commissioning support under an approved test plan. Formal conformance, universal interoperability, production timing, and unrestricted operational-substation use are not claimed.

## Priority 1 — Reporting hardening

- Implement explicit URCB and BRCB lifecycle state machines.
- Improve reservation, ownership, `EntryID`, `PurgeBuf`, overflow, and reconnect handling.
- Validate DataSet member order for every received report.
- Add duplicate, loss, stale timestamp, GI-result, and buffer-recovery diagnostics.
- Export repeatable session evidence without customer or live-network identifiers.

## Priority 2 — Sampled Values analysis

- Add a sustained subscriber and stream registry.
- Add sample-rate detection, payload-layout checks, RMS, phasor, continuity, jitter, dropout, and synchronization evidence.
- Separate ordinary Windows timestamp evidence from any stronger timing claim.
- Add bounded soak tests and deterministic replay fixtures.

## Priority 3 — Simulator and server maturity

- Harden negotiated MMS PDU size and fragmentation handling.
- Complete directory, read, DataSet, and report-control services.
- Add unbuffered reporting, then buffered reporting with recovery evidence.
- Add scenario scheduling for value, quality, timestamp, GOOSE, and Sampled Values changes.
- Keep write and control disabled until read-only services and reporting are stable and independently validated.

## Priority 4 — SCL and station validation

- Deepen type-template and communication resolution.
- Build a station dataflow graph from publishers, DataSets, subscribers, and `ExtRef` mappings.
- Compare SCL, live MMS model, report membership, and observed process-bus traffic.
- Produce explainable findings with explicit source and confidence.

## Priority 5 — Security and robustness

- Add malformed and negative PDU corpora.
- Add resource limits, cancellation, timeout, reconnect, and fuzz-test coverage.
- Add IEC 62351-related diagnostics only when implemented and testable.
- Maintain a clear distinction between cybersecurity, protocol robustness, and operational safety.

## Product-application direction

The reusable stack remains the primary asset. Product applications should consume stable engine contracts and must not duplicate protocol parsing or state machines.

```text
ARIEC61850 engine
├─ protocol codecs and models
├─ client/server services
├─ control and reporting state machines
├─ process-bus diagnostics
├─ simulation
├─ evidence/export
└─ test support

Product applications
├─ discovery and monitoring
├─ simulator
├─ process-bus publisher/analyzer
└─ commissioning workflows
```

## Release gates

A public release may be tagged only when:

| Gate | Required evidence |
|---|---|
| License | Current source and package identify `GPL-3.0-or-later` without ambiguity |
| Build | Clean Windows build succeeds |
| Tests | All automated tests pass |
| Source hygiene | Source-clean verification passes |
| Provenance | Fixtures and assets have documented lawful origin and redistribution rights |
| Claims | README, website, security policy, and maturity matrix describe the same capability boundary |
| Packaging | Release archive contains required license and attribution files and no private evidence |
| Active functions | Control and publishing remain guarded and documented for isolated or approved test environments |

## Wording rule

Use evidence-based terms such as `implemented`, `unit tested`, `loopback verified`, `laboratory exercised`, `partial`, and `not yet validated`. Do not use wording that implies certification, regulatory approval, universal interoperability, autonomous operation, or operational safety unless formal evidence exists for the exact release.
