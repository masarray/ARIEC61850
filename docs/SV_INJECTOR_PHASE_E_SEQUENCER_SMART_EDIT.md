# SV Injector Phase E - Sequencer Smart Edit

This update continues the workspace-based SV injector workflow with focused operator improvements:

- The Ramp States table no longer repeats the selected signal in each row. The active ramp signal is selected once in the Ramp header, while each ramp row only contains the editable time/value profile.
- The State Sequencer table is now directly editable in-place. Voltage, current, angle, frequency, and duration cells accept plain numbers or values with units.
- Sequence cells follow the same operator workflow as Manual and Ramp: edit, press Enter / arrow away / click elsewhere, validate, then format back to `0.000` with unit.
- Invalid sequence values are rejected with a warning and reverted to the last valid value.
- The selected state immediately drives the analog detail editor, phasor view, time-signal preview, and runtime sequencer output.
- Sequencer runtime output now uses the selected state values as absolute test quantities rather than multiplying by the Manual workspace setpoints.
