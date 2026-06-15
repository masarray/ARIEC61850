# SV Injector Manual Console

The SV Publisher desktop app now includes a QuickCMC-style manual injection console for operator-driven sampled-value testing.

## Main workflow

1. Open `SCL / Config` and select an SV stream.
2. Choose the manual output `Set Mode`:
   - `Direct` for phase-to-earth voltage and phase current values.
   - `Line-Line` for V L1-L2, V L2-L3, and V L3-L1 voltage entry with phase current values.
   - `Symmetrical components` for V1/V2/V0 and I1/I2/I0 entry.
3. Use presets from the toolbar or the table context menu.
4. Run `Dry Start` for verification, or arm the NIC and press `Start Injection` for live Ethernet SV publishing.
5. Press `Stop (F6)` for immediate stop.

## Manual table behavior

Operator-entered magnitudes are treated as RMS phasor values. The publisher converts RMS setpoints to instantaneous peak samples when building the SV payload.

The table supports live editing while the publisher is running. With `Auto apply RUN` enabled, magnitude, angle, frequency, and on/off changes are applied to the next generated SV frames. Stream identity, MAC, APPID, VLAN, and selected NIC remain configured through the SCL/config dialog and should be selected before starting the publisher.

## Presets

Right-click the analog output table to access:

- Nominal Value
- Zero
- Equal Magnitudes
- 100% Load
- 50% Load
- Unload (0%)
- Balance Angles
- Reverse Rotation
- Link Frequencies
- Copy Table / Paste Table

## Electrical notes

- Positive sequence uses L1 = 0°, L2 = -120°, L3 = +120°.
- Reverse rotation uses L1 = 0°, L2 = +120°, L3 = -120°.
- Line-line mode derives internal phase-to-earth phasors using a zero-sequence-free reconstruction before the SV payload is generated.
- Symmetrical component mode derives phase values from V1/V2/V0 and I1/I2/I0 before publishing.

## N5.42.3 manual table behavior

The manual analog-output grid now follows a QuickCMC-style commit model:

- Numeric edits are committed on **Enter**, focus loss, or arrow-key cell navigation.
- Invalid input is rejected with an operator warning and the cell returns to the last valid value.
- Display formatting is limited to `#.###` style precision so long floating-point values do not leak into the operator UI.
- During RUN, committed values are projected immediately to the active channel setpoints used by the SV payload loop.
- The **Balance Angles** action is available only from an Angle-cell context menu. The clicked phase is treated as the anchor and is not shifted; the remaining phase angles are recalculated at ±120 degrees relative to that anchor.
- The signal-name scheme can be changed from the Signal-column context menu: ABC, RSTN, L1/L2/L3/E, or raw internal keys.

## N5.42.4 manual-grid correction

- Right-click on an active numeric cell now commits the current edit first, using the same validation path as Enter/LostFocus.
- Numeric cell editing is optimized for fast operator workflow: select a numeric cell and type directly; Enter, arrow keys, mouse move, and right-click all commit the edited value before moving on.
- The context menu is column-aware:
  - Signal: naming scheme only (ABC, RSTN, L1/L2/L3/E, raw keys).
  - Value: value presets only; Equal Magnitudes uses the clicked cell as the reference and applies it to the compatible phase group.
  - Angle: Zero, Line Angle, Balance Angles, Reverse Rotation only.
  - Freq: Nominal Value, DC, Equal Frequencies only.
- Balance Angles is now anchor-based. The clicked angle remains unchanged; the other phases are derived from the clicked phase using the correct positive-sequence phase order.
- The header-level Balance Angles, Dry Start, and Arm NIC controls were removed from the main operator toolbar to reduce visual noise and prevent wrong workflow assumptions.
- Manual values are treated as secondary RMS operator setpoints by default. The SV payload encoder converts RMS phasors to instantaneous samples using dLSB scaling before sending frames.
