# N5.44.4.2 — COMTRADE Reader Build Fix

This patch fixes a C# overload mismatch in `ComtradeReader.ParseTypedChannelCount`.

## Fixed

- `string.EndsWith(char, StringComparison)` is not a valid overload.
- Replaced it with `string.EndsWith(suffix.ToString(), StringComparison.OrdinalIgnoreCase)`.

## Scope

No behavior change to COMTRADE parsing or SV publishing logic.
