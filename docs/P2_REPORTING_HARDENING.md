# P2 — Reporting Hardening

P2 implements Priority 1 of the engine roadmap on top of P1/P1B. The goal is to make report subscription, buffered replay, cleanup, and evidence handling explicit and testable without duplicating the existing MMS decoder or report-control wire implementation.

## Boundary

The existing persistent report monitor remains authoritative for MMS traffic. P2 adds a hardened evidence/state layer around it:

```text
MMS report subscription plan
          |
          v
existing persistent report monitor
          |
          +---- RCB/DataSet write + readback evidence
          +---- decoded InformationReport frames
          +---- cleanup write evidence
          |
          v
MmsReportLifecycleStateMachine
          |
          +---- explicit URCB/BRCB phase
          +---- DataSet ownership
          +---- EntryID/SqNum replay diagnostics
          +---- overflow/timestamp/GI/integrity evidence
          |
          +---- exact DataSet-order validator
          |
          +---- sanitized repeatable evidence export
```

No second BER/MMS report decoder is introduced.

## Explicit lifecycle state

`MmsReportLifecycleStateMachine` records deterministic phase and event evidence for one report-control session. The phases cover discovery, dynamic DataSet preparation, reservation, configuration, enable, monitoring, stopping, disable/release, cleanup, and fault state.

URCB reservation is represented separately from `RptEna`. A cleanup result that leaves an owned reservation held is surfaced as residue instead of being reported as a clean stop.

Dynamic DataSets become `CreatedBySession` only after successful `DefineNamedVariableList` evidence. Deletion changes ownership back only after a successful delete step. A failed delete therefore remains visible as cleanup residue rather than being silently forgotten.

## Buffered replay and reconnect evidence

For BRCB frames, an observed `EntryID` is the preferred replay identity. Duplicate detection uses `EntryID` when present and otherwise falls back to a bounded compound report identity. The remembered replay-key set is capped so long report sessions do not grow memory without bound.

`SqNum` is retained as a diagnostic only. Backward movement is classified as reset/wrap evidence rather than automatic loss, because sequence numbers may restart across attachments. Forward gaps are reported as loss/replay-boundary evidence, not as proof of packet loss.

The lifecycle records:

- accepted and duplicate report counts;
- forward sequence gaps and backward/reset evidence;
- BRCB `BufOvfl` indications;
- `TimeOfEntry` regression evidence when parseable;
- reports carrying general-interrogation and integrity reasons; and
- the last observed `EntryID`, `TimeOfEntry`, and `SqNum` for reconnect reasoning.

`MmsBufferedReportReconnectPlanner` produces a conservative reconnect plan. A valid observed `EntryID` can become a resume candidate only when the discovered BRCB exposes `EntryID`; malformed cursors fail closed. `SqNum` is never promoted to resume authority. P2 does not silently write a reconnect cursor as part of ordinary monitor attach.

## DataSet member-order validation

Every report returned by `ReceiveHardenedPersistentReportMonitorSliceAsync` is checked again against the exact member order in the subscription plan. Validation rejects or flags evidence when:

- an included DataSet index is out of range;
- inclusion indexes are not strictly increasing;
- mapped value count differs from inclusion count;
- a mapped value carries a different DataSet index than the inclusion position;
- an available mapped member reference or functional constraint disagrees with the directory entry; or
- the report mapper already rejected the frame as unmapped.

The hardened receive result exposes the per-frame validations and `HasDataSetOrderViolations`; application consumers can therefore refuse semantically ambiguous process-value projection even when BER decoding itself succeeded.

## PurgeBuf boundary

`PurgeBufferedReportAsync` is intentionally destructive and fail-closed. It is never called by ordinary start, stop, or reconnect logic. The operation requires all of the following before a wire write is attempted:

1. explicit caller acknowledgement that buffered events may be lost;
2. a BRCB, not a URCB;
3. discovered `PurgeBuf` support; and
4. successful live RCB readback showing explicit `RptEna=false`.

After `PurgeBuf=true`, a second RCB snapshot is required before the operation is reported successful. P2 does not automatically re-enable the RCB after purge.

## Hardened session facade

`StartHardenedPersistentReportMonitorAsync`, `ReceiveHardenedPersistentReportMonitorSliceAsync`, and `StopHardenedPersistentReportMonitorAsync` layer lifecycle and semantic evidence over the established persistent monitor. The lifecycle state is tied to the exact monitor session with a weak association, so stopping a monitor does not create a second long-lived session registry.

The established report monitor still owns write ordering, decoder routing, polling fallback, and cleanup traffic.

## Sanitized repeatable evidence

`MmsReportLifecycleEvidenceExporter` projects a lifecycle snapshot into deterministic JSON that intentionally omits:

- IED, RCB, DataSet, and object references;
- IP addresses and other network identifiers;
- EntryID and source timestamps;
- raw payloads and free-text diagnostic messages.

The exported evidence keeps only phase/state, counters, and ordered event kinds/success flags. It is suitable for synthetic/public regression evidence without copying customer or live-network identifiers.

## Regression gates in this phase

P2 tests cover:

- dynamic BRCB DataSet ownership through create, monitoring, and successful cleanup;
- URCB reservation cleanup residue;
- duplicate EntryID detection;
- SqNum gap and reset classification;
- exact versus invalid DataSet inclusion/member order;
- sanitized evidence export with identifier leakage checks;
- reconnect planning that prefers valid EntryID and rejects malformed cursors; and
- PurgeBuf local safety gates before any MMS association/write is required.

## Deliberate non-claims

This phase does not claim universal relay interoperability or formal IEC 61850 conformance. EntryID-based resume execution remains an explicit reconnect workflow rather than an automatic side effect, and `PurgeBuf` remains an opt-in destructive maintenance action. Report diagnostics distinguish observed evidence from stronger conclusions such as network packet loss.
