# Hybrid Report Acquisition Planner

ARIEC61850 P2.2 provides an engine-owned, read-only steady-state acquisition plan for applications that need to combine IEC 61850 reporting with bounded MMS polling fallback.

The planner exists to avoid an all-or-nothing decision such as “one report plan failed, therefore poll every selected signal.” It calculates report coverage per requested typed signal and keeps polling only for the residual set that cannot be covered safely.

## Architecture boundary

ARIEC61850 owns:

- Report Control Block capability and availability evidence;
- static DataSet membership evidence;
- BRCB/URCB selection constraints;
- exact live MMS point resolution for dynamic DataSet members;
- partial static coverage calculation;
- bounded dynamic report planning;
- residual polling classification;
- typed acquisition diagnostics and evidence.

Applications consume the resulting plan. They should not rebuild RCB availability, DataSet safety, reservation, or report-selection heuristics locally.

## Acquisition sequence

The P2.2 plan describes steady state. A fast initial MMS snapshot may still be used by an application so values appear immediately after association. That initial snapshot is separate from this contract.

Steady-state planning proceeds in this order:

1. Check fresh RCB availability evidence.
2. Reuse or enable safe configured static BRCB/URCB instances that cover requested signals.
3. Recalculate the residual requested signal set.
4. Use verified-empty dynamic BRCB/URCB slots only for residual signals that resolve exactly in the live MMS directory.
5. Leave only the remaining residual signals on bounded MMS polling fallback when that fallback is enabled.

The result can therefore contain several simultaneous acquisition segments, for example:

```text
Static BRCB      -> signal set A
Static URCB      -> signal set B
Dynamic URCB     -> signal set C
MMS polling      -> residual set D
```

## Fresh availability is authoritative

A discovered RCB is not automatically usable. Automatic report planning requires current `MmsRcbAvailabilitySnapshot` evidence.

Configured static reports are eligible only when their DataSet binding is known and their DataSet directory is populated. A free static RCB also requires explicit runtime evidence that it is disabled and not reserved by another client. A report already identified as `UsedByCaller` may be reused without rewriting its DataSet, reservation, or `RptEna` state.

A dynamic slot is eligible only when all of the following are proven:

- the live `DatSet` read succeeded;
- `DatSet` is actually empty;
- availability confidence is `Exact` by default;
- `RptEna=false`;
- no owner is present;
- a URCB has explicit `Resv=false`, or a BRCB has explicit `ResvTms=0`.

Busy, unknown, unreadable, unchecked, or reservation-unknown RCBs do not become automatic write plans.

## Static coverage

Static coverage is evaluated per requested typed signal against fresh DataSet member evidence. Typed catalog DataSet membership is preferred when available, while exact normalized design/runtime/MMS references provide supporting identity evidence.

When several safe static RCBs cover the request, planning is deterministic. The current policy prefers the candidate that covers the largest number of still-uncovered requested signals, then an already caller-owned report, then BRCB, then lexical RCB reference order.

Static selection never changes the canonical design identity of a signal. If reconciliation has established an effective alternate MMS reference, that effective reference may prove live DataSet coverage while the canonical reference remains preserved in the typed signal descriptor.

## Dynamic coverage

Dynamic report planning applies only to signals left after static coverage.

A residual signal must resolve exactly against the live MMS directory using an effective, observed, or canonical MMS reference. A literal user-reference lookup is accepted only when it yields exactly one FC-compatible live point. P2.2 does not use fuzzy matching, vendor aliases, or suffix guessing to construct a dynamic DataSet.

Dynamic plans are bounded by `MaxDynamicReportPlans` and `MaxDynamicMembersPerReport` and are grouped by Logical Device domain. The planner emits typed write intent but performs no DataSet or RCB write itself.

## Polling fallback is residual, not absence

`MmsPollingFallback` means only that a requested signal was not safely covered by a report segment in this planning pass. It is not evidence that the signal is absent.

If polling fallback is disabled, the same residual signal becomes `Uncovered`. `Uncovered` also carries no signal-absence meaning.

Protocol-confirmed signal absence remains a separate reconciliation concern; report planning must not infer it from RCB availability or lack of report coverage.

## Typed output

`MmsHybridReportAcquisitionPlan` exposes:

- overall plan status;
- RCB capability counts;
- static BRCB/URCB signal counts;
- dynamic BRCB/URCB signal counts;
- polling residual and uncovered counts;
- acquisition segments;
- per-signal assignments;
- exact RCB and DataSet references;
- activation intent;
- whether a segment requires a write;
- warnings and blockers.

Applications can therefore present acquisition evidence such as `Static BRCB`, `Static URCB`, `Dynamic URCB`, or `MMS polling fallback` without interpreting protocol state themselves.

## Write boundary

P2.2 is a planner, not an executor. It does not reserve an RCB, write `DatSet`, write trigger/optional fields, enable reporting, disable an existing report, or claim another client’s RCB.

A future or existing execution layer must revalidate any write-sensitive preconditions at execution time before mutating the IED. A planning result is evidence for intended acquisition structure, not a durable lease on an RCB.

## Validation boundary

Automated tests prove deterministic software behavior, fail-closed candidate selection, and residual-only polling semantics. Physical IED validation is still required to demonstrate that a particular device accepts the planned RCB/DataSet operations and delivers reports as expected.