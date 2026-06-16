# N5.44.4.1 — COMTRADE Replay Build Fix

This patch fixes a C# string escaping error in `ComtradeChannelMapper.Normalize`.

## Fixed

- `ComtradeChannelMapper.cs` had an invalid string literal when normalizing backslash characters.
- Replaced the invalid `Replace("\", " ")` source text with a correctly escaped C# string literal.

## Scope

No behavior change to COMTRADE parsing or SV publishing logic.
