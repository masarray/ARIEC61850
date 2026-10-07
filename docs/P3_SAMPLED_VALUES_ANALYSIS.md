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
                                   `--> SvEngineeringWindowAnalyzer
                                            `--> SvSignalWindowAnalyzer
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

The engineering-window layer reuses the same tracker rather than implementing a second counter algorithm. An incomplete one-cycle engineering window is discarded on a gap, duplicate, out-of-order transition, restart, channel-metadata change, or timebase/evidence change. A gap/out-of-order/restart sample may begin a new clean window; a duplicate is not counted as a new time sample.

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

## Bounded engineering windows, RMS, and phasor

`SvEngineeringWindowAnalyzer` is the bridge from successfully SCL-bound/scaled projected samples into the existing `SvSignalWindowAnalyzer`. It never decodes payload bytes and never manufactures engineering units. Samples whose projection remains raw-only are intentionally excluded from engineering RMS/phasor state.

The caller must provide `SvEngineeringWindowEvidence` containing a finite positive sample rate and fundamental frequency plus their provenance. `Unknown` and `ProfileInferred` provenance is rejected for these values, so the analyzer never chooses 50 Hz versus 60 Hz itself. The ratio `sampleRateHz / fundamentalFrequencyHz` must resolve to an integral one-cycle sample count within the configured tolerance and bounded maximum before any state is created.

State is bounded at three dimensions: maximum streams, maximum channels per stream, and maximum samples per cycle. The default configuration is 256 streams × 32 channels × 512 samples/cycle, with an additional validation guard that rejects option combinations whose theoretical retained `double` sample buffers could exceed 64 MiB. The analyzer stores only the current incomplete cycle for each active channel; completed cycle history is not retained internally.

Each channel is keyed by the SCL projection element index and guarded by exact signal reference, CDC, engineering unit, scale source, and scale confidence. A metadata change resets the partial waveform and counter state before accepting the new identity. Timebase, nominal-frequency, or counter-wrap evidence changes likewise reset partial state across that stream.

`SvSignalWindowAnalyzer` remains the deterministic one-cycle-window primitive. It receives exactly one complete, continuity-clean engineering window and returns:

- true window RMS;
- fundamental RMS magnitude;
- fundamental phase angle; and
- sample count.

No frequency, channel meaning, CT/VT ratio, engineering unit, or missing sample is guessed by either layer.

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
- raw-only preservation for SCL layouts without an approved engineering-scale rule;
- fail-closed APPID and `confRev` mismatch handling before payload projection;
- one-cycle engineering RMS/phasor from explicitly scaled samples;
- raw-only exclusion from engineering-window state;
- partial-window reset after `smpCnt` gap and duplicate evidence;
- normal counter-wrap continuity;
- channel-metadata change reset;
- fail-closed non-integral samples-per-cycle and inferred-frequency input; and
- bounded engineering stream/channel registries.

These are unit-test and synthetic-fixture claims only. This phase does not claim production timing accuracy, formal conformance, universal device interoperability, or operational-substation validation.

## Remaining P3 work

The per-channel engineering-window slice is now implemented, but the roadmap priority is not complete. Follow-up work should connect application live-capture and PCAP replay byte sources to the established parser plus `SvSustainedObservationSession`, carry explicit timebase/frequency evidence into `SvEngineeringWindowAnalyzer`, add sanitized real/replay validation fixtures and longer bounded soak evidence, and expose evidence/export contracts without retaining customer or live-network identifiers.
