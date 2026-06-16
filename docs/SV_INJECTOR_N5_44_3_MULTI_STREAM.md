# N5.44.3 — Multi-Stream SV Publisher

This release moves ARSVIN/SV Publisher toward a CMC-style digital test-set workflow by allowing up to three independent Sampled Values publishers to run in the same live session.

## What changed

- Added three selectable IED/MU publisher slots:
  - Publisher 1
  - Publisher 2
  - Publisher 3
- Each publisher slot can be enabled or disabled independently.
- Each publisher can select a different SV control block from the loaded SCL file.
- Each publisher stores its own:
  - svID
  - APPID
  - destination MAC
  - source MAC
  - VLAN ID / priority
  - sample rate preset
  - dLSB settings
  - analog output values
- The live runtime now schedules multiple SV streams with independent sample counters.
- The existing PTP monitor / Lab PTP Publisher remains shared for the live session.

## Sample-rate presets

The previous numeric sample-rate box is replaced by a preset dropdown. The presets intentionally cover the commonly needed protection and power-quality style rates used in IEC 61850 SV work:

- 80 samples/cycle @ 50 Hz = 4000 fps
- 80 samples/cycle @ 60 Hz = 4800 fps
- 256 samples/cycle @ 50 Hz = 12800 fps
- 256 samples/cycle @ 60 Hz = 15360 fps
- 96 samples/cycle @ 50 Hz = 4800 fps
- 96 samples/cycle @ 60 Hz = 5760 fps
- 288 samples/cycle @ 50 Hz = 14400 fps
- 288 samples/cycle @ 60 Hz = 17280 fps

The ASDU `smpRate` field is now emitted according to the selected stream's `smpMod`:

- `SmpPerPeriod`: sends samples per cycle.
- `SmpPerSec`: sends samples per second.

## Scope

This patch deliberately focuses only on multi-stream SV publishing. COMTRADE replay is the next targeted release item and is not mixed into this patch.

## Operator workflow

1. Open SCL.
2. Select Publisher 1, 2, or 3.
3. Enable the publisher slot.
4. Select the SV stream for that simulated IED/MU.
5. Configure values in Quick Manual.
6. Switch to the next publisher slot and repeat.
7. Start live injection.

