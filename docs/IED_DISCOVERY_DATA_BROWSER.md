# IED Discovery Data Browser

The IED Discovery harness is intentionally dense and engineering-oriented. The left explorer stops at Data Object level, while the center detail grid renders Data Attributes and read values.

## Expandable detail rows

Manual reads can return structured MMS values. The browser flattens these values into expandable rows:

```text
Pos
  stVal
  q
    Validity
    Overflow
    OutOfRange
    BadReference
    Failure
    OldData
    Inconsistent
    Inaccurate
    Source
    Test
    OperatorBlocked
  t
  Oper
  SBOw
  Cancel
```

This keeps the left navigation clean for large IEDs while making complex DA values readable in the detail pane.

## Enable RCB dialog

Selecting an RCB and clicking **Enable RCB** opens a report dialog with DataSet, trigger options, optional fields, integrity period, and optional GI. The dialog uses the engine's static report planner and guarded report session path. When enabled, the app writes `RptEna=true`, optionally sends `GI=true`, listens briefly, then attempts cleanup with `RptEna=false`.

This remains a validation harness, not a final product UI. Protocol logic stays in `src`.
