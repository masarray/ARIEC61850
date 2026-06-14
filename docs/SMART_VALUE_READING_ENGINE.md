# Smart Value Reading Engine

N5.41.4 adds an engine-owned presentation layer for IEC 61850 value reading. The WPF application remains a thin presenter; it no longer owns the rules for IED naming, logical device aliases, read target ordering, or DO value summaries.

## Why this exists

IEC 61850 data is not naturally flat telemetry. A Logical Node contains Data Objects, and each Data Object contains Data Attributes across Functional Constraints such as ST, MX, CO, CF and DC. Quality (`q`) and timestamp (`t`) are part of the data model and should be displayed as child rows, not as unrelated side columns.

## Engine responsibilities

- Resolve IED display name from live MMS domain names, for example `BAY01CTRL`, `BAY01PROT`, `BAY01MEAS` -> `BAY01`.
- Keep raw MMS logical device domain names intact while showing human-friendly aliases such as `CTRL`, `PROT`, `MEAS`, and `DR`.
- Build smart read plans for selected LN/DO objects using FC-aware targets.
- Generate collapsed DO summaries such as `Pos = intermediate-state`, `Str = true`, and `A = 0 ∠ 0°, 0 ∠ 0°, 0 ∠ 0°`.
- Decode nested `q`, `t`, `Check`, vector, analogue and control structures recursively.
- Raise warning markers only from evidence: quality, timestamp, read failure or binding mismatch.

## UI responsibilities

The WPF app renders the engine result:

- left explorer: IED / LD / LN / DO;
- center grid: `Name | FC | Type | Value`;
- right monitor: pinned values;
- bottom history: status and findings.

The UI must not guess MMS structure child names by position.

## Current scope

This milestone improves identity resolution, LD alias display, LN-level smart DO rows, recursive q/t decoding, and collapsed DO summaries. It is still a preview layer; live report-driven monitor and full control operate remain guarded future milestones.
