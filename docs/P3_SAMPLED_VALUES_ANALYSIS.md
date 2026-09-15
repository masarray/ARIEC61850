# P3 — Sampled Values Analysis

P3 begins the `ROADMAP.md` Sampled Values analysis priority without replacing the established IEC 61850-9-2 parser, payload decoder, SCL binding, or publisher code.

## Engine boundary

P3 now has a sustained, bounded stream-analysis layer plus a source-neutral observation session after successful SV decoding:

```text
Live capture / PCAP replay
        |
        v
existing SV frame parser
        |
        v
SampledValuesFrame
        |
        v
SvSustainedObservationSession
        |
        +--> existing SvStreamObservationManager
        |        `--> profile / SCL comparison
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

`SvSustainedObservationSession` is deliberately downstream of the existing frame parser. It accepts an already parsed `SampledValuesFrame`, sends the frame through `SvStreamObservationManager` first, and advances sustained state only when that canonical observation path accepts the frame. Live capture and PCAP replay therefore share the same parsed-frame admission and stream-identity path. The session does not add a raw capture parser, a second SV decoder, or a second channel model.

`SvSustainedStreamAnalyzer` retains incremental counters and timing statistics rather than packet payload history. Stream cardinality is bounded, idle streams are evicted, and the oldest stream is evicted when the configured registry capacity is reached. The default registry limit is 256 streams.

The stream identity reuses the existing `SvObservedStreamKey` (`APPID`, source/destination MAC, VLAN, `svID`, and DataSet reference), so this phase does not introduce a second stream-identity model.

## Source provenance

`SvObservationInputKind` remains the provenance marker for the established observation path (`LiveCapture` or `PcapReplay`). The new session preserves that marker while making the sustained metrics source-neutral: equivalent parsed frames with equivalent timestamps produce equivalent sustained evidence regardless of whether the caller obtained them from live capture or PCAP replay.

This is an integration boundary, not a claim that the repository now contains every live-capture or PCAP-file reader required by a production application. Capture-specific byte acquisition remains outside this analyzer and must feed the established SV parser before entering the session.

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

The observation session intentionally does not turn `SamplePayload` bytes into engineering channels. That remains the responsibility of the existing payload decoder plus explicit SCL/profile/measurement context. Unknown channel identity, scale, ratio, or engineering unit must remain unresolved rather than being inferred from word position.

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
- host-vs-hardware timestamp claim boundaries;
- RMS/fundamental phasor accuracy for a deterministic sine wave;
- one-ingress coordination of observation and sustained snapshots;
- equivalent sustained metrics for equivalent live-capture and PCAP-replay parsed frames;
- fail-closed behavior when the canonical observation path rejects an empty-payload frame; and
- a 10,000-frame synthetic ingestion regression that keeps the sustained registry at its configured stream bound.

These are unit-test and synthetic-fixture claims only. This phase does not claim production timing accuracy, formal conformance, universal device interoperability, or operational-substation validation.

## Remaining P3 work

The roadmap priority is not complete. Follow-up work should connect application live-capture and PCAP replay byte sources to the established parser plus `SvSustainedObservationSession`, project explicitly SCL-bound and engineering-scaled channel windows through the existing payload decoder into the RMS/phasor primitive, add sanitized real/replay validation fixtures and longer soak evidence, and expose evidence/export contracts without retaining customer or live-network identifiers.
