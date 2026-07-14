# Full Stack Development Plan

This document expands the future work described in the root [Roadmap](../ROADMAP.md). Completed work is summarized in [Changelog](../CHANGELOG.md), and current evidence is recorded in [Engine Maturity Matrix](ENGINE_MATURITY_MATRIX.md).

## Product direction

ARIEC61850 is developed as a reusable IEC 61850 engine with thin engineering applications. Protocol codecs, state machines, binding, diagnostics, simulation, and evidence export remain in reusable libraries.

```text
Engine contracts
→ CLI and laboratory harnesses
→ stable product applications
```

## Current workflow baseline

### IED discovery

```text
Connect
→ discover live model
→ inspect DataSets and RCBs
→ read typed values
→ build evidence
```

### Reporting

```text
Read current RCB state
→ classify readiness and ownership
→ validate DataSet membership
→ build a typed plan
→ confirm writes
→ enable and monitor
→ stop and clean up
```

### Guarded control

```text
Select control Data Object
→ discover ctlModel and live MMS types
→ verify approved test conditions
→ stage operator intent
→ confirm
→ execute Direct or SBO sequence
→ separate acceptance, termination, application error, and feedback
```

### Process bus

```text
Load synthetic or approved SCL
→ derive expected streams
→ inspect PCAP or live capture
→ bind expected and observed traffic
→ diagnose sequence, timing, configuration, and payload findings
→ export evidence
```

### Simulator

```text
Load deterministic profile
→ expose read-only MMS model
→ exercise discovery and reads
→ record activity and evidence
```

## Development tracks

### Track A — Reporting reliability

- explicit URCB and BRCB lifecycle models;
- ownership and reservation evidence;
- reconnect, duplicate, loss, and stale-data handling;
- `EntryID`, overflow, purge, and resume strategy;
- long-duration monitor and cleanup tests.

### Track B — Sampled Values receive and analysis

- sustained live subscriber;
- stream registry and SCL binding;
- payload-layout validation;
- continuity, duplicates, ordering, and wrap analysis;
- RMS, phasor, frequency, jitter, dropout, and synchronization evidence;
- bounded performance and soak tests.

### Track C — Simulator reporting

- unbuffered report generation;
- buffered report generation and recovery state;
- deterministic scenario scheduling;
- quality and timestamp changes;
- GOOSE and Sampled Values scenario coordination;
- write/control only after read and reporting behavior is mature and reviewed.

### Track D — Station engineering

- deeper SCL type-template resolution;
- communication and access-point validation;
- publisher/DataSet/subscriber graph;
- SCL versus live MMS comparison;
- SCL versus observed process-bus comparison;
- explainable findings with source and confidence.

### Track E — Robustness and security

- malformed and oversized input testing;
- resource and queue limits;
- timeout, cancellation, reconnect, and fault cleanup;
- fuzzing for codecs and parsers;
- release dependency review;
- security-profile work only when implementation and evidence exist.

## Application boundaries

### IED Discovery

Primary role:

- live model browsing;
- typed reads;
- report setup and monitoring;
- guarded control;
- evidence export.

The application must not duplicate protocol parsing, FC resolution, report state, control sequencing, or value binding.

### Engineering Workbench

Primary role:

- read-only SCL and PCAP analysis;
- expected-vs-observed findings;
- loopback readiness checks;
- evidence-pack export.

### IED Simulator

Primary role:

- deterministic laboratory model;
- read-only MMS server;
- scenario and activity evidence.

It is not presented as a production IED or formal conformance reference.

### Sampled Values Publisher

Primary role:

- bounded laboratory publishing;
- waveform and phasor preparation;
- dry-run and adapter verification;
- visible active-output state.

## Evidence priorities

Every development track should add a deterministic path:

```text
synthetic input
→ engine service
→ typed result
→ explicit finding or state
→ Markdown/JSON evidence
```

Live equipment testing supplements deterministic tests; it must not be the only evidence.

## Claim policy

Use precise evidence wording. Do not describe a feature as safe, certified, compliant, production-ready, universally interoperable, or field-proven unless the exact claim is supported by documented evidence for the exact release.

The phrase “laboratory exercised” means only that a controlled test path was completed under a recorded procedure. It does not imply operational-substation approval.
