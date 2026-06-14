# IED Discovery Report Value Projector

`N5.42.1` moves report presentation from raw MMS value rows to an IEC 61850 engineering signal model.

## Why this matters

A report value is not just a scalar. The report payload is ordered by the active DataSet members. A member can be a leaf data attribute such as `XCBR1.Pos.stVal`, or a structured Data Object such as `PTOC1.Str`. The UI must not show `Struct(...)` as the primary monitor value when the structure can be projected into readable signals.

## Static reporting

For static report control blocks, the engine reads `DatSet`, reads the DataSet directory, enables the RCB, then maps each received report value by DataSet member index.

## Dynamic reporting

For dynamic slots, the engine builds a temporary DataSet from pinned signals or selected priority points, writes that DataSet reference to the RCB, enables the RCB, and removes the temporary DataSet on stop when safe.

## Activity Monitor policy

When an RCB monitor is running:

- report-covered signals are updated from report frames;
- polling is only used as fallback for pinned signals that are not covered by the active report DataSet;
- structured report values are projected into readable child signals;
- quality and timestamp are attached to the engineering signal row where possible.

Example projection:

```text
A50PTOC1.Str  Struct(10)
```

becomes:

```text
A50PTOC1.Str.general  false  q=good  t=2026-06-13 14:58:23.144
A50PTOC1.Str.phsA     true   q=good  t=2026-06-13 14:58:23.144
A50PTOC1.Str.phsB     false  q=good  t=2026-06-13 14:58:23.144
A50PTOC1.Str.phsC     false  q=good  t=2026-06-13 14:58:23.144
```

## Safety boundary

The monitor keeps `RptEna=true` until the user explicitly clicks **Stop RCB**, closes the IED, or the session faults. Stop attempts to write `RptEna=false`, release reservation, and delete temporary dynamic DataSets created by this session.
