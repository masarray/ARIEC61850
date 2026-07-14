# SCL DataSet binding states

ARIEC61850 keeps ReportControl DataSet evidence explicit instead of inferring validity from `Entries.Count`.

`SclReportControl.DataSetBindingStatus` has four states:

- `NotSpecified` — the control block has no `datSet` attribute.
- `Unresolved` — a reference is present, but no unique DataSet matches the same IED, logical device and logical-node scope.
- `ResolvedEmpty` — the DataSet was resolved, but it contains no `FCDA` members.
- `Resolved` — the DataSet was resolved and contains one or more members.

The resolver accepts common SCL and MMS-style reference forms while keeping resolution bounded to the owning IED:

- `Events`
- `LLN0$Events`
- `LD0/LLN0$Events`
- `IED1LD0/LLN0$Events`
- `IED1/LD0/LLN0.Events`

Engineering findings use the typed state:

- indexed, unassigned RCB: `SCL_REPORT_DATASET_UNASSIGNED` (`Warning`)
- non-indexed RCB without `datSet`: `SCL_REPORT_DATASET_MISSING` (`High`)
- unresolved reference: `SCL_REPORT_DATASET_UNRESOLVED` (`Warning`)
- resolved DataSet with zero members: `SCL_REPORT_DATASET_EMPTY` (`High`)

This prevents an unresolved vendor reference from being reported as a genuinely empty DataSet.
