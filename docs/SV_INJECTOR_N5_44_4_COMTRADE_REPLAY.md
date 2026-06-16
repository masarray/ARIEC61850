# N5.44.4 — COMTRADE Replay for SV Injection

This patch adds the first COMTRADE replay path to the SV Publisher workspace.

## Scope

N5.44.4 intentionally supports the field-useful minimum first:

- COMTRADE `.cfg` + `.dat` pair
- ASCII `.dat`
- analog channels only for replay
- automatic channel mapping to `Va`, `Vb`, `Vc`, `Vn`, `Ia`, `Ib`, `Ic`, `In`
- one selected IED / MU publisher at a time
- one-shot replay by default, optional loop replay

Binary DAT, CFF, multi-rate records, manual channel remapping UI, and digital channel replay are intentionally left for later.

## Operator flow

1. Open SCL.
2. Select Publisher 1 / 2 / 3.
3. Select the target SV stream.
4. Click **Comtrade**.
5. Pick the `.cfg` file; the matching `.dat` must be next to it.
6. Start injection.

The selected publisher will inject instantaneous COMTRADE analog samples as Sampled Values. Other enabled publishers can still use manual phasor values.

## Scaling rule

COMTRADE analog values are converted with the standard linear channel coefficients:

`engineering_value = raw_value * a + b`

ARSVIN then converts the instantaneous engineering value into SV integer counts using the configured `dLSB` value.

## Safety note

This is a replay engine for R&D and lab use. It does not make ARSVIN a calibrated test set and does not replace a certified protection test set or external PTP grandmaster.
