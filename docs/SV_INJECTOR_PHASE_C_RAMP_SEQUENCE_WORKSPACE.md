# SV Injector Phase C - Ramp and State Sequence Workspace Refinement

This phase continues the QuickCMC-style workspace direction introduced in Phase B.

## Operator UX changes

- Ramp Assessment panel removed.
- Ramp Detail View simplified to Analog Out only.
- State Sequence General and Trigger tabs removed.
- State Sequence now shows the time signal view and selected-state phasor at the same time.
- Selecting a state card updates the selected-state phasor and Analog Out detail immediately.
- Ribbon buttons now use larger outline-style icons with smaller captions for faster visual scanning.

## Preview model

The workspace now exposes separate preview channel collections:

- `RampPreviewChannels`
- `SequencePreviewChannels`

These are display/preview collections, not the live SV publish channel collection. They allow Ramp and State Sequence to preview selected-step values without corrupting Manual mode live setpoints.

## Notes

Phase C is still conservative on runtime engine changes. It focuses on UI ownership, selected-state preview, and operator clarity. The next phase can bind Ramp and State Sequence execution more deeply to the publisher loop.
