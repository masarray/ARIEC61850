# P3 — Sampled Values Analysis

P3 begins the `ROADMAP.md` Sampled Values analysis priority without replacing the established IEC 61850-9-2 parser, payload decoder, SCL binding, or publisher code.

## Engine boundary

The first P3 slice adds a sustained, bounded stream-analysis layer after successful SV decoding:

```text
Ethernet / PCAP input
        |
        v
existing SV frame parser
        |
        v
SampledValuesFrame
        |
        +--> existing profile / SCL comparison
        |
        `--> SvSustainedStreamAnalyzer
                 |
                 +--> bounded stream registry
                 +--> smpCnt continuity / missing-sample evidence
                 +--> arrival-rate / jitter / dropout evidence
                 +--> payload-length stability
                 +--> smpSynch observations
                 `--> bounded snapshots for applications / evidence export
```

`SvSustainedStreamAnalyzer` retains incremental counters and timing statistics rather than packet payload history. Stream cardinality is bounded, idle streams are evicted, and the oldest stream is evicted when the configured registry capacity is reached. The default registry limit is 256 streams.

The stream identity reuses the existing `SvObservedStreamKey` (`APPID`, source/destination MAC, VLAN, `svID`, and DataSet reference), so this phase does not introduce a second stream-identity model.

## Continuity evidence

Sample-counter continuity reuses `SvSampleCounterTracker`. P3 records continuous transitions, normal wraps, forward gaps, missing-sample counts, duplicates, and out-of-order transitions. A caller may supply a trusted/configured counter modulus; otherwise the existing tracker remains profile-neutral.

A sample-counter gap means samples were not observed by this analyzer. It is evidence of an observation gap, not by itself proof of physical-network packet loss.

## Timing evidence and claim boundary

P3 records mean frame interval, observed frame rate, estimated sample rate, arrival-interval standard deviation, maximum observed arrival jitter, and large arrival intervals.

Timestamp provenance is explicit:

- `HostSoftwareTimestamp` is ordinary host/capture-arrival evidence only.
- `HardwareTimestamp` records that the caller supplied hardware timestamp evidence, but does **not** by itself prove clock accuracy, PTP synchronization quality, or IEC 61850 process-bus timing performance.
- `Unknown` makes no stronger timing claim.

Applications must carry this claim boundary forward. A Windows or other ordinary host timestamp must never be relabeled as precision synchronization evidence.

## Synchronization evidence

The analyzer counts observed `smpSynch` values (0, 1, 2, and other values) exactly as decoded. This is wire evidence. P3 does not infer clock quality, grandmaster identity, or station synchronization health solely from these counts.

## Payload-layout evidence

For a stable stream identity, P3 records the first ASDU payload length and marks the stream when later ASDUs change length. This complements the existing SCL-aware payload decoder and configuration comparer; it does not guess signal semantics from raw word positions.

## RMS and phasor primitive

`SvSignalWindowAnalyzer` provides deterministic one-cycle-window RMS and fundamental phasor calculation for an explicitly supplied numeric sample window. It intentionally accepts already mapped/scaled numbers rather than raw SV payload bytes. The caller must establish channel identity, engineering scale, and timebase from SCL or other explicit trusted context before presenting engineering-unit RMS or phasor results.

The fundamental estimator returns:

- true window RMS;
- fundamental RMS magnitude;
- fundamental phase angle; and
- sample count.

No frequency, channel meaning, CT/VT ratio, or engineering unit is guessed by this primitive.

## Deterministic regression coverage

Synthetic tests cover:

- sustained 4 kHz stream-rate estimation;
- continuous sample counters and synchronization observations;
- sample-counter gaps and missing-sample totals;
- arrival-time dropout diagnostics with explicit non-network-loss wording;
- bounded stream-registry eviction;
- payload-length changes inside one stream identity;
- host-vs-hardware timestamp claim boundaries; and
- RMS/fundamental phasor accuracy for a deterministic sine wave.

These are unit-test and synthetic-fixture claims only. This phase does not claim production timing accuracy, formal conformance, universal device interoperability, or operational-substation validation.

## Remaining P3 work

The roadmap priority is not complete with this first slice. Follow-up work should connect the bounded analyzer to a sustained live subscriber/capture loop and PCAP replay path, add bounded soak/replay fixtures, project SCL-backed channel windows into the RMS/phasor primitive, and expose sanitized evidence/export contracts without retaining customer or live-network identifiers.
