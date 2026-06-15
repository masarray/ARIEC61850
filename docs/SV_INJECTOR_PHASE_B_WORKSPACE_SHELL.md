# SV Injector Phase B - Workspace Shell

Phase B converts the SV Injector from a dialog-based utility into a mode-based test workspace.

## What changed

- The main window now switches the central workspace between:
  - Quick Manual
  - Ramping
  - State Sequencer
- Ramp and State Sequence are no longer opened as small setup dialogs from the toolbar.
- The top command bar now behaves like a test-set workspace selector.
- Manual workspace remains the fast QuickCMC-style analog output editor from Phase A.
- Ramp workspace now has:
  - ramp-state editor
  - signal view
  - ramp timeline preview
  - detail view
  - assessment table placeholder
- State Sequencer workspace now has:
  - horizontal state table
  - selected-state detail editor
  - time-signal preview
  - phasor tab

## Scope boundary

This phase is mainly structural UI refactoring. The existing publisher engine remains intact:

- Manual mode continues to publish the live manual setpoints.
- Ramp mode still uses the existing ramp engine fields (`SelectedRampChannel`, `RampTargetMagnitude`, `RampDurationSeconds`).
- Sequencer mode still uses the existing sequence engine fields (`SequenceStates`, `LoopSequence`).

Phase C should connect richer ramp/state table editing directly into the runtime engine and add better assessment logic.
