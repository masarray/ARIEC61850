# SV Injector Phase A Manual Polish

This phase tightens the manual injection console so it behaves more like a fast relay-test-set workspace.

## Operator editing

- Numeric cells accept plain numbers or values with units.
- Commit is triggered by Enter, arrow-key navigation, losing focus, or right-click context actions.
- Valid committed values are displayed using fixed three-decimal formatting:
  - `57.740 V`
  - `1.000 A`
  - `50.000 Hz`
  - `-120.000 °`
- Invalid text is rejected and the previous valid value is restored.

## Grid presentation

- The separate Unit column was removed.
- Units are now part of the committed display text in the editable field.
- The grid remains optimized for keyboard-first operation.

## Phase colors

Phasor and waveform views now share the same phase identity palette:

- R / A / L1: red
- S / B / L2: yellow/amber
- T / C / L3: blue
- N / residual / zero sequence: gray

Voltage and current use the same phase color by design, so phase identity stays consistent across the phasor and waveform views.
