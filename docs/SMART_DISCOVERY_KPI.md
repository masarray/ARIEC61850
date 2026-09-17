# Smart Discovery KPI / Capture Convergence

Status: **implemented + unit tested; live capture convergence not yet re-validated after this patch**.

This document defines the P0-4 observability contract for smart IEC 61850 discovery. The KPI layer is diagnostic evidence only. It does not create a second IEC 61850 model, does not alter canonical semantics, and does not add MMS traffic.

## Architecture

```text
existing smart discovery requests
        |
        +-- GetNameList
        +-- GetNamedVariableListAttributes
        +-- GetVariableAccessAttributes
        `-- bounded FC-root Read
        |
        v
existing confirmed-service execution
        |
        +------------------------+
        |                        |
        v                        v
canonical discovery/model   zero-traffic KPI recorder
                             counts / latency / duplicate keys
                             peak outstanding / completeness
                             deterministic signature
```

The canonical IED model remains the only semantic source of truth. KPI fields are an evidence overlay and are never used to manufacture LD/LN/DO/DA, DataSet, RCB, value, type, or SCL semantics.

## Zero-traffic invariant

KPI collection must not issue any additional:

- `GetNameList`;
- `GetVariableAccessAttributes`;
- `Read`;
- `GetNamedVariableListAttributes`;
- association request;
- report/control request;
- transport probe.

Observations are opened only around confirmed requests that the smart path already intends to send.

## Observed phases

The optimized default path records these wire request classes:

| Phase | MMS service | Logical duplicate key |
|---|---|---|
| `structure` | `GetNameList` | object class + domain/VMD + continuation token |
| `dataset-directory` | `GetNamedVariableListAttributes` | exact DataSet reference |
| `type-enrichment` | `GetVariableAccessAttributes` | exact domain/item reference |
| `initial-read` | `Read` | ordered FC-root reference batch |

A repeated normalized logical key in one generation increments `DuplicateRequests`. Legitimate GetNameList continuation pages are distinct because the continuation token is part of the key.

## Snapshot contract

`MmsClientSession.LastSmartDiscoveryKpi` exposes a point-in-time `MmsSmartDiscoveryKpiSnapshot` containing:

- total / successful / failed observed confirmed requests;
- duplicate request count;
- peak outstanding request count;
- per-phase request count and latency statistics;
- LD, LN, raw-variable and FC-point completeness;
- DataSet / DataSet-directory / ordered member completeness;
- RCB / BRCB / URCB counts;
- `WireAccountingComplete` plus explicit `AccountingNotes`;
- deterministic signature.

`RefreshSmartDiscoveryModelKpi(directory)` can refresh LD/LN/FC-point completeness after later semantic materialization. It is local bookkeeping and performs no network I/O.

## Deterministic signature

The signature intentionally excludes:

- wall-clock timestamps;
- measured request latency;
- worker completion order;
- invoke ID allocation order.

It includes normalized request identity/attempt outcomes, accounting coverage, and semantic completeness counts. Equivalent evidence should therefore produce the same signature even when concurrent workers finish in a different order.

This signature is a convergence diagnostic, not an IEC 61850 semantic fingerprint and not an SCL identity.

## Accounting coverage

The default optimized smart-discovery path keeps report attribute probing off the structural critical path. For that path, all currently instrumented smart request loops can report complete wire accounting.

If `ProbeReportAttributes=true` and report controls are eligible for probing, the current legacy report-enrichment Read fallback path is not yet observed at individual wire-attempt granularity. The snapshot therefore sets:

```text
WireAccountingComplete = false
```

and adds an explicit accounting note. This prevents an undercount from being presented as complete evidence.

## Current controlled convergence target

For the current controlled regression device/session, the project-level target supplied to P0 is:

```text
LD                    32
LN                   119
semantic leaves     4,925
DataSet                2
ordered FCDA           58
logical ReportControl  32
```

These values are capture-specific regression evidence, not IEC 61850 limits and must never be hard-coded into protocol behavior.

P0-4 live acceptance requires a new controlled run to demonstrate, for the same evidence set:

1. expected canonical completeness is retained;
2. `DuplicateRequests == 0` unless a documented retry/fallback is expected;
3. `WireAccountingComplete == true` for the optimized default path;
4. repeated runs yield the same deterministic signature when semantic evidence and outcomes are unchanged;
5. request count and per-phase latency are recorded without additional MMS traffic;
6. DataSet member order and RCB semantics remain unchanged.

## What is validated now

- **Implemented:** zero-traffic KPI recorder and smart call-site observations.
- **Unit tested:** deterministic signature independent of completion order; duplicate detection; peak outstanding count; model-completeness refresh; explicit partial accounting.
- **CI validated:** build/test/source-verification must pass on the exact PR head before this phase is called code-complete.
- **Not yet laboratory re-validated:** final request counts, latency distribution, duplicate count, and canonical completeness against a fresh controlled packet capture after this patch.

## Next phase: P0-5 Request Budget & Adaptive Convergence

P0-5 may consume P0-4 evidence to reduce redundant requests and tune bounded concurrency. It must not optimize from guesses. Any request removal or concurrency change must preserve canonical completeness, DataSet ordering, report/control safety, deterministic publication order, and negotiated `maxOutstandingCalling` limits.

P0-5 candidate gates:

- establish per-phase request budget from controlled repeat runs;
- identify only proven duplicate/redundant semantic requests;
- adapt window conservatively from negotiated peer limits and observed latency/failure evidence;
- compare before/after request count and wall time with identical canonical signature/completeness;
- fall back to the conservative path when evidence is insufficient.
