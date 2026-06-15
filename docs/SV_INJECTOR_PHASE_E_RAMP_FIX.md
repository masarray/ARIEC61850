# SV Injector Phase E - Ramping Correction

This phase tightens the Ramping workspace around a practical time-based SV injection workflow.

## Changes

- Removed the operator-facing run-behavior checkboxes. The publisher runs continuously and committed table edits are always applied live.
- Reworked the Ramping header to use Test Universe-style fields:
  - Set mode
  - Fault type (`n/a`)
  - Estimated test time
  - Signal 1
  - Quantity 1
  - Signal 2 disabled
  - Quantity 2 disabled
- Added a curated Signal 1 list that matches the current engine:
  - `V L1-E`
  - `V L2-E`
  - `V L3-E`
  - `I L1`
  - `I L2`
  - `I L3`
  - `V L1-E, L2-E, L3-E`
  - `I L1, L2, L3`
- Ramp segment selection now stores explicit channel keys. `I L1` maps only to `Ia`; three-phase current maps to `Ia,Ib,Ic`; three-phase voltage maps to `Va,Vb,Vc`.
- Fixed Ramp publish/preview logic so the active ramp overrides only the selected signal keys. All other analog outputs keep their editable Analog Out values.
- Made the Ramp Detail View editable by binding it to the same validated Analog Out rows used by Manual mode.

## Model rule

Ramp mode is time-based only. There are no relay-response transition fields in the operator workflow.
