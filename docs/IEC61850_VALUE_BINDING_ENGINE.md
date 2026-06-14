# IEC 61850 Value Binding Engine

This milestone moves Data Attribute rendering from value-driven expansion to schema-driven IEC 61850 semantic binding.

## Why this matters

MMS `Structure` values are positional. A user interface must not assume that child index 0 is `stVal`, index 1 is `q`, or index 2 is `t` unless an IEC 61850 schema confirms that order. The binding engine therefore combines live discovery metadata, functional constraints, inferred CDC, and built-in CDC templates before rendering values.

## Binding sources

The engine uses the following order:

1. Live MMS model and variable type discovery when available.
2. Live discovery DA list grouped by LD/LN/DO/FC.
3. SCL-derived type hints when available in the document workflow.
4. Conservative built-in CDC templates for well-known structures such as DPC, SPC, ACT, ACD, WYE, MV, quality, timestamp, origin, and control operation values.
5. Raw positional fallback with low-confidence diagnostics only when no schema can be resolved.

## Output contract

The engine returns a canonical bound value tree:

```text
BoundValueRow
  Name
  Reference
  FunctionalConstraint
  Type
  Value
  Quality
  Timestamp
  Status
  SemanticKind
  Confidence
  Children[]
```

WPF only renders this tree. It does not guess IEC 61850 structure names.

## Current coverage

N5.41.2 adds:

- Data Object schema building from live discovery.
- CDC-aware ordering for DPC/SPC/ACT/ACD/WYE/MV-style objects.
- Quality bit-string decoding into named flags.
- UTC timestamp decoding into readable time and time-quality rows.
- Control operation structure binding for `SBOw`, `Oper`, and `Cancel`.
- Origin structure binding for `orCat` and `orIdent`.
- Control model enum labels such as `sbo-with-enhanced-security`.
- Binding mismatch diagnostics when schema child count and MMS value count differ.

## UI behavior

The IED Discovery Workbench now builds the middle detail panel from schema first. Selecting a DO produces one root row with child DA rows. Manual read expands readable rows and binds returned MMS values back to the schema.

