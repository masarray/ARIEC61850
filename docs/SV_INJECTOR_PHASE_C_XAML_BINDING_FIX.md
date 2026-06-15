# SV Injector Phase C XAML Binding Fix

This patch fixes the Phase C runtime XAML exception where display-only read-only properties such as `VoltageMagnitudeText`, `CurrentText`, `FrequencyText`, `MagnitudeText`, `AngleDegreesText`, and `FrequencyHzText` were bound by WPF controls using their default TwoWay binding mode.

## Fix

- Display-only `Run.Text` bindings in the State Sequencer card now use `Mode=OneWay`.
- Display-only `DataGridTextColumn` bindings in Ramp and State Sequence preview grids now use `Mode=OneWay`.
- Editable fields are left unchanged.

## Reason

Some WPF text-bearing elements and generated templates can request TwoWay source updates. That is invalid for read-only calculated properties. Using explicit `Mode=OneWay` is the correct approach for calculated display text.
