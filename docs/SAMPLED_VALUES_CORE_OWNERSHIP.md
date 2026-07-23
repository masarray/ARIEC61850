# Sampled Values core ownership

## Decision

`ARIEC61850` is the single source of truth for reusable IEC 61850 protocol, Sampled Values decoding, engineering analysis, measurement-domain conversion, diagnostics, and evidence contracts.

Derived applications such as ARSVIN Publisher and ArSubsv Subscriber may provide WPF presentation, commands, file dialogs, adapter selection, orchestration, and user-facing formatting. They must not fork or silently modify protocol and measurement behavior in application repositories.

## Core imported from the ARSVIN application repository

The following reusable components were present in the embedded `ARSVIN.Engine` copy but absent from `ARIEC61850/main` during the July 2026 ownership audit:

- evidence-driven engineering scaling;
- profile-neutral `smpCnt` transition tracking;
- timebase resolution without a hidden 50/60 Hz assumption;
- explicit CT/VT ratio and primary/secondary-domain conversion;
- semantic Sampled Values quality decoding;
- versioned stream measurement-context JSON;
- generic ASDU inspection;
- generic `seqOfData` word inspection with preserved trailing bytes.

These components are vendor-neutral. Manufacturer or product identity must never select a parser, dataset order, scaling rule, quality interpretation, timebase, or health result.

## Ownership boundary

### ARIEC61850

- Ethernet, VLAN, APPID, BER, APDU, and ASDU codecs;
- Sampled Values frame parsing and building;
- SCL parsing and ordered FCDA/DataSet mapping;
- generic raw payload inspection;
- quality semantics;
- continuity, timebase, scaling, CT/VT, waveform, RMS, phasor, noise-floor, and signal-validity analysis;
- protocol, stream, configuration, and measurement health contracts;
- PCAP and Npcap transport abstractions;
- reusable evidence and comparison models;
- deterministic protocol and engineering tests.

### Derived applications

- WPF controls and windows;
- ViewModels and commands;
- selected-stream state and UI refresh policy;
- adapter and file-selection workflows;
- plot rendering and display formatting;
- application settings, branding, and packaging;
- application smoke and presentation tests.

## Integration contract

Local application development uses sibling repositories:

```text
<workspace>/
├── ARIEC61850/
└── arsvin/
```

Application CI must pin and checkout a reviewed ARIEC61850 commit or branch as a sibling. It must not build against a moving, unrecorded engine revision.

The migration is intentionally staged. Existing embedded source can remain temporarily for comparison, but it must be removed from active project references. New reusable IEC 61850 logic belongs here first.
