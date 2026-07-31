# AR Virtual Relay Lab — Product Requirements Document

Status: execution baseline  
Target: Windows 10/11 x64, .NET 8 WPF  
License: GPL-3.0-or-later  
Primary engine: ARIEC61850  
Subscriber UX and workflow reference: ARSVIN / ArSubsv

## Product statement

AR Virtual Relay Lab is a process-bus protection engineering workbench in which every protection decision is traceable from an incoming IEC 61850 Sampled Values stream through measurement, protection logic, trust gating, pickup, operate and virtual trip.

It is not a dashboard, a vendor-relay clone, a certified protection IED, a conformance test set, or a physical trip device.

## Product promise

The main workspace must show the complete cause-and-effect chain without vertical page scrolling:

```text
SMV waveform changes
→ operating quantity changes
→ protection element picks up
→ timer or inverse accumulator advances
→ trip permission is evaluated
→ virtual relay operates or displays an explicit block reason
```

The same workspace must make communication failure equally understandable:

```text
SMV integrity degrades
→ trust guard identifies the exact reason
→ measurement remains visible when safe to inspect
→ trip permission is removed
→ no silent or false trip is produced
```

## Users

- protection and control engineers;
- substation automation and IEC 61850 engineers;
- FAT, SAT and commissioning engineers;
- lecturers, students and protection researchers;
- engineers developing or comparing numerical-relay algorithms.

## V1 scope

### Inputs

- live IEC 61850 Sampled Values capture through the ARIEC61850 Npcap transport;
- classic PCAP replay;
- deterministic internal waveform and fault scenarios;
- SCL/SCD/CID/IID-assisted stream binding;
- manual mapping when engineering metadata is unavailable;
- primary and secondary CT measurement context.

### Protection

- 50P phase instantaneous overcurrent;
- 51P phase time overcurrent;
- 50N/50G instantaneous earth fault;
- 51N/51G time earth fault;
- phase-segregated pickup indication;
- common guarded virtual-trip matrix;
- definite-time and IEC inverse-time operation;
- dropout, reset, memory and latch behavior;
- per-element editable algorithm contract.

### Evidence

- sequence of events;
- sample-counter correlation;
- source, stream and SCL identity;
- settings snapshot;
- active algorithm identity and content hash;
- pickup, operate, trip and blocking reasons;
- waveform and protection trace export;
- optional source-PCAP correlation.

## Out of scope for V1

- physical output contacts;
- active GOOSE trip output;
- MMS breaker control;
- operational-substation deployment;
- protection certification or IEC 61850 conformance claim;
- vendor firmware, vendor algorithm or vendor faceplate replication;
- distance, differential, directional and autoreclose protection.

## UX contract

### Visual character

The application must be clean, lean and industrial rather than decorative:

- compact one-screen engineering workspace;
- neutral off-white shell and graphite text;
- restrained blue selection accent;
- green used only for healthy or permitted states;
- amber used only for pickup, caution and blocking;
- red used only for trip or error;
- thin dividers, small radii and minimal shadows;
- no glassmorphism, oversized typography, bulky cards or excessive bold text;
- engineering values use a monospaced typeface;
- the virtual relay is original and vendor-neutral.

### Main workspace

Optimized viewport: 1440 × 900.  
Minimum supported viewport: 1280 × 720.

```text
┌─────────────────────────────────────────────────────────────────────┐
│ product identity │ source │ stream │ SCL │ algorithm editor │ run │
├───────────────────────────────────────────┬─────────────────────────┤
│ SMV waveform — fixed two-cycle window     │ original virtual relay  │
│ IA / IB / IC / 3I0                        │ LCD measurements         │
│ pickup and trip markers                   │ healthy/pickup/trip LEDs │
│                                            │ phase and earth LEDs     │
├───────────────────────────────────────────┤ trust and block reason  │
│ protection element causality strip        │ keypad and reset         │
│ 50P · 51P · 50N · 51N · event trace       │ virtual-output boundary  │
└───────────────────────────────────────────┴─────────────────────────┘
```

The left workspace receives approximately 64–66% of width; the relay receives 34–36%. Splitters may resize the areas, but the relay must remain fully visible.

### Waveform behavior

- exactly two cycles based on estimated or configured frequency;
- stationary oscilloscope view rather than a continuously travelling decorative waveform;
- rolling sample replacement may occur internally, but the displayed time reference remains fixed;
- fixed fault-inception, pickup and trip markers;
- IA, IB, IC and 3I0 traces;
- primary/secondary view selection;
- freeze, triggered and trip-centred modes in later V1 increments;
- rendering refresh is independent from protection calculation.

### Virtual relay behavior

The relay faceplate is constructed from vector controls, not a vendor image. It includes:

- neutral device identity and laboratory serial;
- LCD pages for current, protection, process-bus health, active blocking and last event;
- Healthy, Pickup, Trip, Phase A/B/C, Earth Fault and Block indicators;
- navigation controls and explicit reset;
- persistent trip latch until reset;
- no network or physical output in V1.

## Architecture

```text
WPF application shell
  ├── workspace and virtual-relay presentation
  ├── project/scenario orchestration
  ├── evidence recorder
  └── immutable UI snapshots
          ↑
Protection engine
  ├── 50P / 51P / 50N / 51N
  ├── timers and inverse accumulators
  ├── trip matrix and latch
  └── algorithm runtime contract
          ↑
Measurement pipeline
  ├── channel mapping and scaling
  ├── RMS/fundamental extraction
  ├── residual current and frequency
  └── circular disturbance buffer
          ↑
SMV trust guard
  ├── stream identity and freshness
  ├── smpCnt continuity and duplicates
  ├── sample rate and processing budget
  ├── quality and synchronization policy
  └── allowsMeasurement / allowsPickup / allowsTrip
          ↑
ARIEC61850
  ├── Sampled Values codec and profiles
  ├── SCL
  ├── PCAP
  └── Npcap transport
```

Protection evaluation must never depend on UI refresh cadence. Capture, decoding, measurement, protection, evidence and UI presentation are separate bounded work domains.

## SMV trust model

The trust guard produces three explicit permissions:

```text
AllowsMeasurement
AllowsPickup
AllowsTrip
```

Default behavior is conservative and explainable. A condition may remain measurable while trip is blocked, but the block reason must be visible and recorded.

Minimum supervised conditions:

- APPID, destination MAC, VLAN and svID identity;
- confRev change;
- payload mapping and scaling;
- smpCnt continuity, duplicate and reorder;
- sample-rate stability;
- frame freshness and stream stall;
- quality policy;
- synchronization state;
- decode and processing overrun;
- algorithm runtime health.

## Measurement model

Canonical current signals:

```text
IA
IB
IC
IN
3I0 = IA + IB + IC
```

Each signal retains raw representation, scale, engineering unit, secondary value, primary value, CT ratio, mapping source and quality.

Built-in measurement choices:

- full-cycle RMS;
- half-cycle RMS;
- sliding RMS;
- full-cycle fundamental Fourier;
- half-cycle fundamental Fourier;
- custom typed measurement algorithm.

## Protection contract

### 50P and 50N

- configurable pickup;
- minimum persistence and definite delay;
- dropout ratio and reset delay;
- phase or residual source selection;
- mandatory trust permission before virtual trip.

### 51P and 51N

- definite time;
- IEC Standard, Very, Extremely and Long-Time Inverse;
- numerical time integration rather than a one-shot timer;
- instantaneous, definite, inverse or memory reset;
- independently configured phase and earth-fault settings.

## Algorithm Editor

Algorithm editing is per element and uses a typed, unit-aware protection DSL. Arbitrary C# execution is not the standard mode.

Activation workflow:

```text
edit
→ syntax and unit validation
→ bounded-runtime analysis
→ deterministic tests
→ reference/custom comparison
→ stage
→ shadow evaluation
→ explicit activation
```

Safe Laboratory Mode enforces `smv.allowsTrip` as a mandatory final gate. Algorithms cannot access files, networks, processes, reflection, unmanaged code, UI objects or unbounded loops. Every staged or active revision has a version and content hash.

Example:

```text
element "50P-1" {
  input phaseCurrent = max(IA.rms1c, IB.rms1c, IC.rms1c)
  pickup = phaseCurrent >= setting("I>>")
  operate = pickup.persist(setting("Delay"))
  trip = operate && smv.allowsTrip
}
```

## Deterministic scenario coverage

- balanced load;
- load step and overload;
- three-phase, phase-phase and phase-earth fault;
- inception-angle and DC-offset variation;
- harmonics, noise and CT-saturation approximation;
- frequency deviation and current ramp;
- isolated gap, burst loss, duplicate and reorder;
- stale stream, freeze, APPID/svID/confRev change;
- quality invalid and synchronization loss;
- processing overrun and algorithm runtime failure.

## Acceptance criteria

- the main workspace requires no vertical page scrolling at 1280 × 720;
- the waveform represents a fixed two-cycle timebase;
- live, replay and simulated sources enter the same measurement/protection contract;
- 50/51 phase and earth-fault outputs are generated only from measurement frames;
- repeated replay of identical evidence produces identical event ordering and operation time within the defined tolerance;
- uncertain input removes trip permission and displays the exact reason;
- the UI cannot stall or change protection timing;
- algorithm source with errors or missing trust gate cannot be staged;
- trip indication remains latched until reset;
- no active network or physical trip output exists in V1;
- product artwork and naming remain original and vendor-neutral.

## Delivery sequence

### P0 — implemented shell

- original premium one-screen WPF workspace;
- stationary two-cycle waveform control;
- neutral virtual-relay faceplate;
- deterministic current frames;
- basic 50/51 phase and earth-fault core;
- SMV trip-permission demonstration;
- pickup, trip, block and reset causality;
- focused algorithm-editor prototype.

### P1 — real subscriber pipeline

- extract and integrate ArSubsv live capture and PCAP replay workflow;
- SCL stream binding and channel mapping;
- measurement context and scaling;
- real circular sample buffers;
- deterministic protection tests and evidence events.

### P2 — complete algorithm laboratory

- typed DSL parser, type system and bytecode/interpreter;
- unit and static-safety analysis;
- shadow A/B execution;
- reference comparison and activation control;
- versioned algorithm packages and content hashes.

### P3 — evidence and release quality

- disturbance recorder and COMTRADE workflow;
- JSON/Markdown evidence pack;
- performance and replay benchmarks;
- installer, portable package, SBOM and checksums;
- professional documentation and GitHub Pages product site.

## Safety statement

AR Virtual Relay Lab is an engineering and educational laboratory. It is not a certified relay, deterministic real-time protection platform or authorization to operate primary equipment. V1 produces virtual trip indications only.
