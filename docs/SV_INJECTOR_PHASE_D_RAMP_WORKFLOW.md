# SV Injector Phase D - Ramping Workflow

Phase D turns the Ramping workspace into a time-based operator table instead of a dialog-style setup form.

## Operator workflow

- Select **Ramp** from the ribbon.
- Edit the **Test View: Ramping** table directly.
- Press **Enter**, use arrow keys, click another cell, or right-click to commit the edited value.
- Valid values are formatted back with units such as `5.000 A`, `57.735 V`, `0.100 s`, and `0.200 A/s`.
- Invalid values show a warning and revert to the last accepted value.
- Right-click the ramp table for **Append Ramp** and **Delete Ramp**.

## Time-only ramp model

The ramp no longer exposes relay-response transition fields. Each ramp segment is executed by time only:

- `From`
- `To`
- `Step`
- `dt`
- `Steps`
- `Time`

During Ramp mode publishing, the active ramp segment is resolved from elapsed time. The selected signal is interpolated linearly from `From` to `To` within that segment duration. Other channels keep their current manual setpoints.

## Removed UI noise

- Relay-response transition fields were removed.
- Ramp assessment remains removed.
- Detail View remains analog-output only.
