# N5.44.4.3 — COMTRADE Binary DAT Support

This patch removes the runtime blocker when importing COMTRADE records whose CFG declares `BINARY`.

## Added

- COMTRADE `BINARY` DAT reader.
- Tolerant support for `BINARY32` and `FLOAT32` records.
- Binary record parsing:
  - 32-bit sample number
  - 32-bit timestamp
  - analog values
  - digital status words skipped for now
- Engineering scaling remains `value = raw * a + b`.
- Added `samples/comtrade/simple_fault_binary.cfg` and `.dat` for smoke testing.

## Scope

N5.44.4.x still replays analog channels only. Digital channel replay, manual channel remapping, and CFF support remain intentionally out of scope for the first public ARSVIN SV injector release.
