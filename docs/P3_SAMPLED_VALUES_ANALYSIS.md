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
        +--> SvSustainedStreamAnalyzer
        |        +--> bounded stream registry
        |        +--> smpCnt continuity / missing-sample evidence
        |        +--> arrival-rate / jitter / dropout evidence
        |        +--> payload-length stability
        |        `--> smpSynch observations
        |
        `--> optional explicit SCL profile
                 `--> existing SampledValuesPayloadDecoder
                          `--> SvSclBoundMeasurementProjector
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

When no explicit `SampledValuesPublisherProfile` is supplied, `SamplePayload` remains opaque to the observation session. When a profile is supplied, `SvSclBoundMeasurementProjector` reuses that profile's existing `SampledValuesPayloadLayout` and `SampledValuesPayloadDecoder`; it does not infer a new layout from packet shape.

## Evidence-backed channel projection

`SvSclBoundMeasurementProjector` provides the conservative bridge from an explicitly bound SCL publisher profile into numeric channel samples. Before decoding it requires exact APPID, destination MAC, VLAN, `svID`, DataSet reference, and `confRev` compatibility. Unsupported SCL layout elements or a payload-length mismatch fail closed rather than shifting offsets and continuing with guessed semantics.

Numeric values always retain their decoded raw value. Engineering values are emitted only when the existing `SvEngineeringScaleResolver` has sufficient SCL-backed evidence. The currently supported installed-base scaling rule additionally requires the exact fixed eight-value/eight-quality, 64-byte layout, four SCL-derived current channels, four SCL-derived voltage channels, and protection-rate evidence. Other valid SCL layouts remain raw-only until a separate explicit scale rule exists.

CT/VT conversion is also explicit. An optional `SvStreamMeasurementContext` is accepted only when its validated stream key and optional `svID` match the observed stream. Primary/secondary-equivalent values are then produced through `SvMeasurementDomainResolver`; a missing, invalid, or mismatched context never causes a ratio to be guessed.

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
- fail-closed behavior when the canonical observation path rejects an empty-payload frame;
- a 10,000-frame synthetic ingestion regression that keeps the sustained registry at its configured stream bound;
- fixed SCL-bound current/voltage projection using the existing payload decoder;
- explicit primary-to-secondary CT/VT display conversion;
- raw-only preservation for SCL layouts without an approved engineering-scale rule; and
- fail-closed APPID and `confRev` mismatch handling before payload projection.

These are unit-test and synthetic-fixture claims only. This phase does not claim production timing accuracy, formal conformance, universal device interoperability, or operational-substation validation.

## Remaining P3 work

The roadmap priority is not complete. Follow-up work should add bounded per-channel engineering windows that feed `SvSignalWindowAnalyzer` only from successfully scaled projections, connect application live-capture and PCAP replay byte sources to the established parser plus `SvSustainedObservationSession`, add sanitized real/replay validation fixtures and longer soak evidence, and expose evidence/export contracts without retaining customer or live-network identifiers.
