# IED Discovery Reporting Repair

N5.41.7 improves the IED Discovery workbench reporting workflow.

## What changed

- RCB selection now refreshes the live RCB runtime attributes before showing the detail view or planning an enable operation.
- The report detail panel is driven from the live `MmsReportControlCandidate` state when a live session is available.
- Static reporting is planned from the refreshed `DatSet` value and a verified DataSet directory.
- Dynamic reporting is planned only when the refreshed RCB is an empty slot.
- GI is attempted when requested even when the discovery attribute list did not expose the `GI` child; unsupported GI writes are recorded as guarded warnings.
- Report group panels now show RCB status summaries, including busy/locked, static DataSet, and dynamic slot state.
- Activity Monitor now has explicit Pin and Unpin actions.

## Safe workflow

1. Select an RCB.
2. The workbench refreshes `RptEna`, `DatSet`, `RptID`, `ConfRev`, `BufTm`, `TrgOps`, `OptFlds`, and reservation state.
3. The user opens Enable RCB.
4. The workbench refreshes the RCB again before planning because another client can claim it while the dialog is open.
5. The planner chooses static DataSet mode or dynamic DataSet mode from the latest state.
6. Guarded enable writes `RptEna=true`, optionally triggers GI, listens for a short evidence window, then attempts cleanup.

This remains a guarded engineering alpha. Long-running report sessions are planned for the next monitor milestone.
